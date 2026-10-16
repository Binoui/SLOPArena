# EXE Release & Self-Hosted Infrastructure Implementation Plan
> **Historical execution plan.** Preserved as a dated decision/implementation record, not a current checklist. Current product work follows the [playable friends demo reset](2026-09-05-playable-demo-reset.md). Revalidate historical status claims against current code before reuse.


> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship a downloadable Windows `.exe` that non-technical friends can run to play training mode solo or join online games, backed by a self-hosted master server + dedicated game servers on the home mini PC.

**Architecture:** Three layers — the Windows Unity player (built locally, self-contained game server binary bundled for host-and-play), the master server + PostgreSQL (ASP.NET Core 8 on the Debian 12 mini PC, TLS-terminated by Cloudflare Tunnel at `sloparena.barakaslurp.fr`), and 1-2 dedicated game server instances (`src/Server`, docker-compose-managed) that players join directly over UDP. Host-and-play stays supported for technical players who port-forward; non-technical players only join the operator's OfficialServers. All services run as docker containers (host network) — the mini PC hosts no .NET install; binaries are published on the dev machine and rsync'd.

**Tech Stack:** Unity 6000.0.78f1 (Windows player build), .NET 8 (game server, master server — dev machines only, containers use `mcr.microsoft.com/dotnet/aspnet:8.0`), PostgreSQL 15 (container), Cloudflare Tunnel (TLS + ingress), docker compose (service management), GitHub Actions (CI test gate), GitHub Releases (distribution).

## Global Constraints

- **Audience:** friends-first demo (not public release). Non-technical users must only ever click: download → unzip → run → Training or Join. No port forwarding, no config, no .NET install.
- **Hosting model (grill Q1):** hybrid — dedicated servers are the default online path; host-and-play is supported for technical players. "Host" is NOT a player verb in user docs.
- **Mini PC (grill Q2):** Debian 12 bookworm, kernel 6.12.74+deb12, 16GB DDR4. **Runs the user's home automation** — all deployment steps must be additive and non-destructive; containers get `restart: unless-stopped` and resource caps (`mem_limit: 512m`, `cpus: 0.5`); never touch existing postgres configs/data — the app database is its own `postgres:15` container with a named volume.
- **Domain (grill Q3):** `barakaslurp.fr` owned; release builds point at `https://sloparena.barakaslurp.fr` (subdomain to be created). Plain HTTP is NOT acceptable for the master server — Cloudflare Tunnel terminates TLS at the edge (no Caddy, no inbound 443 needed).
- **Roster (grill Q5):** all characters legal in online matches. `custom_rules` is omitted from official `server.json` (note: `AllowedCharacters` is currently decorative server-side — no enforcement exists; do not build enforcement in this plan).
- **Signing (grill Q6):** none — friends-only. SmartScreen click-through documented in the player guide.
- **Distribution (grill Q7):** GitHub Releases zip. itch.io is post-demo.
- **Versioning (grill Q8):** semver, `v0.2.0-demo.1` style; `productName` becomes `SLOPArena`, `bundleVersion` stamped per build.
- **CI (grill Q9):** CI runs the 390-test .NET suite (this repo + master repo). The exe is built locally by `scripts/build-release.sh`. Master server deploy is scripted (rsync/ssh), not CI-triggered — home infra is not CI-reliable.
- **Uptime (grill Q10):** low uptime acceptable; CGNAT/inbound ports UNVERIFIED — Phase 0 is a hard gate; if CGNAT is found, STOP and fall back to a VPS.
- **Reconciliation (grill Q4):** client prediction/rollback is NOT implemented (netcode Phase 1, roadmap Phase 7). A remote playtest in Phase 7 decides whether it's required before the real demo release; if so, a separate plan is written (this plan gates the *friends* release on the playtest result, not on reconciliation).
- **Unity editor path:** `/home/binoui/Unity/Hub/Editor/6000.0.78f1/Editor/Unity`.
- **Commits:** conventional, squash per `docs/contributing/conventions.md`. Never push without permission.
- **Windows players only** for the demo exe. The Linux player build stays a dev artifact.

---

## Phase 0 — Network & DNS readiness (hard gate)

### Task 0.1: Verify inbound reachability (CGNAT check)

**Files:** none (router/DNS).

- [x] **Step 1: Compare public IP vs router WAN IP**

```bash
curl -4 ifconfig.me
```

Log into the router admin panel and read the WAN IP. If they differ → **CGNAT. STOP.** Fall back to a VPS (Hetzner CX22, ~4€/mo) and redeploy Phases 1-4 there — document the alternative in `docs/systems/production-hosting.md` as a note.

- [x] **Step 2: Port-forward the game range on the router**

Forward to the mini PC's LAN IP (only the game-server range — TLS/HTTPS goes through Cloudflare Tunnel, which is outbound-only):
- TCP `7777` and UDP `7777-7791` (game server instance 1: MatchControlServer TCP + 15 match ports)

Note: the Bbox router only allows forwards in 1024-8191, so the old TCP 80/443 plan (Caddy) would not work here anyway — Cloudflare Tunnel sidesteps it entirely.

Verify from OUTSIDE the LAN (phone on 4G, not Wi-Fi):

```bash
# TCP 7777 (expect connection refusal or hang, NOT timeout — timeout = blocked)
nc -vz <public-ip> 7777
```

- [x] **Step 3: Create the tunnel hostname (replaces DNS record)**

No A record at the registrar — Cloudflare Tunnel provisions DNS automatically. In the Cloudflare dashboard (Zero Trust → Networks → Tunnels → the existing alfred tunnel → Public Hostname → Add):

```
Subdomain: sloparena
Domain:    barakaslurp.fr
Service:   HTTP → localhost:5000
```

Save; Cloudflare creates the CNAME. Verify:

```bash
curl -s https://sloparena.barakaslurp.fr/health
# {"status":"ok","version":"0.1.0"}  ← after Phase 2
```

- [ ] **Step 4: Commit**

No repo change; record results in `docs/systems/production-hosting.md` (created in Task 6.3 — note the phase order: 0.4 is a stub that happens when the doc exists; verify the doc records the check result).

---

## Phase 1 — Mini PC base services

### Task 1.1: Postgres 15 container + compose stack dir

**Files:** alfred: `/root/homelab/sloparena/{docker-compose.yml,.env}` (compose file versioned on the box — it is deliberately NOT in this repo; game repo stays game-only. Template is maintained directly on alfred; `.env` copied from `.env.example` with a generated password).

No .NET on the host; no apt postgres — the box's home-automation packages stay untouched. Postgres runs as an official `postgres:15` container with a named volume; role + database come from env (`POSTGRES_USER/PASSWORD/DB`). The master and game-server services are in the same compose file (created now, started when their binaries land in Phases 2/4).

- [x] **Step 1: Create the stack dir + secrets**

```bash
sudo mkdir -p /root/homelab/sloparena
# scp from dev machine: docker-compose.yml + .env.example (from alfred's own copy — see note above)
# .env: SLOPARENA_DB_PASSWORD="$(openssl rand -base64 18)"   (chmod 600)
```

- [x] **Step 2: Start postgres only**

```bash
cd /root/homelab/sloparena
docker compose up -d postgres
```

- [x] **Step 3: Verify**

```bash
# verified: sloparena-postgres Up; SELECT 1 → 1 (host-reachable via SSH tunnel on 15432)
```

```bash
docker compose ps            # sloparena-postgres Up
PGPASSWORD='<generated>' psql -h 127.0.0.1 -U sloparena -d sloparena -c "SELECT 1;"   # → 1
```

---

## Phase 2 — Master server deploy

### Task 2.1: Publish from the dev machine + rsync + migrate

**Files:**
- Dev machine: `/home/binoui/Documents/projects/SlopArena-MasterServer` (publish source)
- Mini PC: `/srv/sloparena/master/publish/` (binaries **and** `appsettings.Production.json` inside it — the compose file bind-mounts the whole dir read-only and cannot overlay a single file onto it; a file mount fails at runtime with `create mountpoint ... read-only file system`)

No build on the mini PC — the master repo is published locally and rsync'd, same pattern as the game server. Postgres is already up (Phase 1) and reachable at `127.0.0.1:5432` on the host.

- [x] **Step 1: Publish + rsync (dev machine)**

```bash
cd ~/Documents/projects/SlopArena-MasterServer
dotnet publish -c Release -o /tmp/minipc-master   # /tmp, NOT build/ — see the ⚠ warning below (test-project bin/obj leak)
dotnet tool install --global dotnet-ef --version 8.0.0   # once; for migrations
ssh alfred 'sudo mkdir -p /srv/sloparena/master && sudo chown alfred:alfred /srv/sloparena/master'
rsync -avz build/minipc-master/ alfred:/srv/sloparena/master/publish/
# ⚠ never add --delete to this rsync: it would wipe the config written in Step 2.
#    --delete-excluded is EVEN WORSE — it deletes files excluded from transfer
#    (this actually happened 2026-08-02: config was destroyed and had to be
#    rebuilt; JWT secret was regenerated). Also publish master to /tmp, not
#    build/minipc-master: the MasterServer.Tests subfolder leaks its bin/obj
#    into the output (content globs) and grows recursively on re-publish → MSB3030.
```

- [x] **Step 2: Write production config (mini PC) — INSIDE publish/**

`/srv/sloparena/master/publish/appsettings.Production.json` (content on the box — never commit secrets):

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=sloparena;Username=sloparena;Password=<from Phase 1 .env>"
  },
  "Jwt": {
    "Secret": "<openssl rand -base64 64>"
  },
  "Urls": "http://127.0.0.1:5000"
}
```

```bash
sudo chmod 600 /srv/sloparena/master/publish/appsettings.Production.json
```

- [x] **Step 3: Apply migrations (one-time, from dev machine)**

The containerized postgres publishes only on `127.0.0.1` — reach it through an SSH tunnel. Use a non-conflicting local port (this dev machine runs its own postgres on 5432):

```bash
ssh -f -N -L 15432:127.0.0.1:5432 alfred   # local 15432 → alfred's container postgres
# dev machine:
cd ~/Documents/projects/SlopArena-MasterServer
export ConnectionStrings__DefaultConnection='Host=localhost;Port=15432;Database=sloparena;Username=sloparena;Password=<from Phase 1 .env>'
dotnet ef database update
# expected: "Done."
# verify (through the same tunnel):
PGPASSWORD='<password>' psql -h localhost -p 15432 -U sloparena -d sloparena -c "\dt"   # GameServers, Users, Matches
```

(Note: the master container's own `appsettings.Production.json` keeps `Host=localhost;Port=5432` — that points at the host's 5432, which is the container's published port on alfred. The 15432 only exists on the dev machine for the migration tunnel.)

### Task 2.2: docker compose service

**Files:** `/root/homelab/sloparena/docker-compose.yml` on the mini PC (versioned on the box, not in this repo).

- [x] **Step 1: Deploy the compose file**

```bash
# dev machine
rsync -avz docker-compose.yml alfred:/root/homelab/sloparena/   # from wherever the template is kept (currently only on alfred)
ssh alfred 'cd /root/homelab/sloparena && docker compose up -d master'
```

The `master` service runs `mcr.microsoft.com/dotnet/aspnet:8.0` with `network_mode: host` (cloudflared is also host-networked → reaches `127.0.0.1:5000`), bind-mounts `/srv/sloparena/master/publish` read-only, resource-capped (`mem_limit: 512m`, `cpus: 0.5`) — same caps as the original systemd unit, minus `NoNewPrivileges`/user sandboxing (root inside container; host exposure is host-network).

- [x] **Step 2: Verify locally**

```bash
curl -s http://127.0.0.1:5000/health   # {"status":"ok","version":"0.1.0"}
docker compose logs master | tail
```

### Task 2.3: Cloudflare Tunnel hostname (replaces Caddy)

**Files:** Cloudflare Zero Trust dashboard (no install — the alfred tunnel already runs as a container).

- [x] **Step 1: Add the public hostname** (Zero Trust → Networks → Tunnels → alfred's tunnel → Public Hostname → Add):

```
Subdomain: sloparena
Domain:    barakaslurp.fr
Service:   HTTP → localhost:5000
```

- [x] **Step 2: Verify from outside (phone 4G)**

```bash
curl -s https://sloparena.barakaslurp.fr/health
# {"status":"ok","version":"0.1.0"}  ← proves tunnel + TLS + NAT-less reachability + master all work
```

### Task 2.4: Backups (demo-scale)

**Files:** `/etc/cron.d/sloparena-backup` on the mini PC. Same weekly pg_dump; the container's volume holds the data, the dump goes to the host FS.

- [x] **Step 1: Weekly pg_dump**

```
30 4 * * 1 root mkdir -p /var/backups/sloparena && docker exec sloparena-postgres pg_dump -U sloparena sloparena | gzip > /var/backups/sloparena/sloparena-$(date +\%F).sql.gz && find /var/backups/sloparena -name '*.sql.gz' -mtime +90 -delete
```

- [x] **Step 2: Verify**

```bash
sudo ls -la /var/backups/sloparena 2>/dev/null || echo "run once manually: sudo bash -c 'docker exec sloparena-postgres pg_dump -U sloparena sloparena | gzip > /var/backups/sloparena/manual-test.sql.gz'"
```

---

## Phase 3 — Code fixes (release blockers)

### Task 3.1: ADR-0009 — demo hosting model

**Files:** Create `docs/adr/0009-demo-hosting-model.md`.

- [x] **Step 1: Write the ADR** (amends ADR-0005's "LAN/localhost sufficient" posture):

```markdown
# ADR-0009: Demo Hosting Model — OfficialServers + Technical Host-and-Play

**Status:** Accepted — 2026-08-02
**Deciders:** @Binoui
**Amends:** ADR-0005 (embedded host-and-play demo posture)

## Context

ADR-0005 chose embedded host-and-play for the demo, noting "LAN/localhost is
sufficient." The demo goal is friends over the internet. Player-hosted matches
are unreachable from outside a LAN today: `GameServerRegistration` advertises
the machine's LAN IP, and remote reachability requires per-host port
forwarding — a non-starter for non-technical players.

## Decision

Two-tier hosting for the demo:
1. **OfficialServers** — operator-run dedicated `GameServer` instances on the
   home mini PC (always on, registered with a reachable public IP/domain).
   Non-technical players only Join; no NAT work on their side.
2. **HostAndPlay** — stays supported for technical players who port-forward.
   The player sets their public IP/domain in the host UI; the bundled
   self-contained server binary is spawned from StreamingAssets.

## Consequences

- Player-facing docs use "Join" (OfficialServers) as the primary path; "Host"
  is documented separately for technical users.
- The game server must accept a `publicIp` override (domain allowed) in
  `server.json`; `ServerHost` must launch a bundled binary in release builds.
- The master server URL in release builds points at `https://sloparena.barakaslurp.fr`.
- Future migration to VPS hosting is infra-only (ADR-0005 consequence unchanged).
```

- [x] **Step 2: Commit**
git commit -m "docs: add ADR-0009 demo hosting model + glossary terms"
```

### Task 3.2: Fix `server.json` key case + add `publicIp` override

**Files:**
- Modify: `src/Server/MultiMatchOrchestrator.cs:124-149` (ServerConfig class)
- Modify: `src/Server/GameServerRegistration.cs:48`
- Modify: `src/Shared/HostedServerConfig.cs`
- Modify: `src/Server/server.json`
- Test: `tests/Shared.Tests/HostedServerConfigTests.cs`

**Interfaces:**
- Consumes: `ServerConfig` (PascalCase props, case-insensitive JSON binding), `HostedServerConfig` (camelCase emission).
- Produces: `ServerConfig.PublicIp` (nullable string, JSON key `publicIp`), `HostedServerConfig.PublicIp` (nullable string), used by Task 4.1's `server.json` and by `ServerHost` (Task 3.3).

- [x] **Step 1: Add `PublicIp` to ServerConfig**

In `src/Server/MultiMatchOrchestrator.cs`:

```csharp
public string MasterServerUrl { get; set; } = "http://localhost:5000";
/// <summary>
/// Public IP or DNS name advertised to the master server (clients connect
/// here over UDP). Null → auto-detect LAN IP (correct only for directly
/// routable machines). Set behind NAT (e.g. "sloparena.barakaslurp.fr").
/// </summary>
public string? PublicIp { get; set; }
```

- [x] **Step 2: Use it in registration**

In `src/Server/GameServerRegistration.cs:48`, change:

```csharp
var ip = GetPublicIpAddress();
```

to:

```csharp
var ip = _config.PublicIp ?? GetPublicIpAddress();
```

- [x] **Step 3: Rewrite `src/Server/server.json` in camelCase + publicIp**

The current snake_case keys do NOT bind (`PropertyNameCaseInsensitive` handles case only, not underscores — `master_server_url`, `custom_rules` etc. silently fell back to defaults). New canonical dev file:

```json
{
  "serverName": "SLOPArena Local Dev",
  "region": "EU",
  "port": 7777,
  "maxConcurrentMatches": 15,
  "masterServerUrl": "http://localhost:5000",
  "isOfficial": false,
  "arenaDataDir": "data/arenas"
}
```

(`publicIp` omitted → LAN auto-detect; keep dev behavior unchanged.)

- [x] **Step 4: Add `PublicIp` to HostedServerConfig + test**

In `src/Shared/HostedServerConfig.cs`:

```csharp
/// <summary>
/// Public IP/domain for the browser list when the host machine is behind NAT.
/// Null → the server advertises its LAN IP (LAN-only hosting).
/// </summary>
public string? PublicIp { get; init; }
```

In `tests/Shared.Tests/HostedServerConfigTests.cs`, add:

```csharp
[Fact]
public void ToJson_WithPublicIp_EmitsCamelCasePublicIp()
{
    var cfg = new HostedServerConfig { PublicIp = "sloparena.barakaslurp.fr" };
    var json = cfg.ToJson();

    Assert.Contains("\"publicIp\": \"sloparena.barakaslurp.fr\"", json);
}
```

- [x] **Step 5: Build + test**

```bash
dotnet build src/Shared/ --nologo
dotnet test tests/Shared.Tests/ --nologo   # all pass, incl. new test
dotnet build src/Server/ --nologo
```

- [x] **Step 6: Smoke — server honors publicIp**

```bash
cd src/Server && dotnet run --no-build -- '{"serverName":"smoke","port":7777,"maxConcurrentMatches":1,"masterServerUrl":"http://localhost:5000","publicIp":"1.2.3.4"}' 2>/dev/null || true
# or: write the object to /tmp/smoke.json and pass the path; watch the log line:
#   [Registration] ... ipAddress 1.2.3.4  ← proves override wins
```

- [x] **Step 7: Commit**

```bash
git add src/Shared/HostedServerConfig.cs src/Server/MultiMatchOrchestrator.cs src/Server/GameServerRegistration.cs src/Server/server.json tests/Shared.Tests/HostedServerConfigTests.cs
git commit -m "fix(server): honor publicIp override in server.json (issue #52)"
```

### Task 3.3: ServerHost — launch bundled server binary in release builds

**Files:**
- Modify: `client/Unity/Assets/Scripts/Runtime/Network/ServerHost.cs` (`StartHosting`, `ResolveRepoRoot` area)

**Interfaces:**
- Consumes: `HostedServerConfig.PublicIp` (Task 3.2), bundled binary at `StreamingAssets/Server/SlopArena.Server[.exe]` (produced by Task 5.3).
- Produces: editor behavior unchanged; built players spawn the self-contained binary with the config path as `args[0]` (matches `Program.cs:12`).

- [x] **Step 1: Rewrite the launch branch in `StartHosting`**

Replace the `_assignedPort = FindFreeUdpPort();` … `Process.Start(psi);` block's path resolution:

```csharp
string bundledServerDir = Path.Combine(Application.streamingAssetsPath, "Server");
string bundledExe = Path.Combine(bundledServerDir,
    Application.platform == RuntimePlatform.WindowsPlayer ? "SlopArena.Server.exe" : "SlopArena.Server");
bool useBundled = File.Exists(bundledExe);

string repoRoot = ResolveRepoRoot();
string arenaDir = useBundled
    ? Path.Combine(Application.streamingAssetsPath, "arenas")
    : Path.IsPathRooted(_arenaDataDir)
        ? _arenaDataDir
        : Path.GetFullPath(Path.Combine(repoRoot, _arenaDataDir));

if (!Directory.Exists(arenaDir))
{
    _pending.Enqueue(() => RegistrationFailed?.Invoke($"Arena data dir not found: {arenaDir}"));
    return;
}

_assignedPort = FindFreeUdpPort();
var config = new HostedServerConfig
{
    ServerName = serverName,
    Region = "EU",
    Port = _assignedPort,
    MaxConcurrentMatches = 1,
    MasterServerUrl = _masterServerUrl,
    IsOfficial = false,
    ArenaDataDir = arenaDir
};

_configPath = Path.Combine(Path.GetTempPath(), $"sloparena-host-{_assignedPort}.json");
File.WriteAllText(_configPath, config.ToJson());
UnityEngine.Debug.Log($"[ServerHost] Wrote config to {_configPath} (port {_assignedPort})");

ProcessStartInfo psi;
if (useBundled)
{
    psi = new ProcessStartInfo
    {
        FileName = bundledExe,
        ArgumentList = { _configPath },
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        CreateNoWindow = true,
        WorkingDirectory = Path.GetDirectoryName(bundledExe)!
    };
}
else
{
    string projectPath = Path.IsPathRooted(_serverProjectPath)
        ? _serverProjectPath
        : Path.GetFullPath(Path.Combine(repoRoot, _serverProjectPath));
    psi = new ProcessStartInfo
    {
        FileName = _dotnetPath,
        ArgumentList = { "run", "--project", projectPath, "--", _configPath },
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        CreateNoWindow = true,
        WorkingDirectory = repoRoot
    };
}
```

(`ResolveRepoRoot` stays as-is; it is only consulted in the editor fallback branch now.)

- [x] **Step 2: Editor regression check**

Run the game from the Editor (`Arena_PvP` scene → Host flow). Expected: falls back to `dotnet run` (no `StreamingAssets/Server` yet), server registers, host plays on localhost.

- [x] **Step 3: Commit**

```bash
 git add client/Unity/Assets/Scripts/Runtime/Network/ServerHost.cs
git commit -m "fix(client): spawn bundled server binary in release builds (issue #52)"
```

### Task 3.4: Consolidate master server URL for release builds

**Files:** Modify 4 defaults → `https://sloparena.barakaslurp.fr`:
- `client/Unity/Assets/Scripts/Runtime/ClientSession.cs:17,75`
- `client/Unity/Assets/Scripts/Runtime/UI/MainMenuController.cs:14`
- `client/Unity/Assets/Scripts/Runtime/UI/ServerBrowserUI.cs:17`
- `client/Unity/Assets/Scripts/Runtime/Network/ServerHost.cs:41`

- [x] **Step 1: Check for scene inspector overrides first**

`grep -rn "localhost:5000" client/Unity/Assets/Scenes/ --include="*.unity"` — if scene serialized values exist, update them too (scene values win over code defaults).

- [x] **Step 2: Change all defaults to `https://sloparena.barakaslurp.fr`**

Dev machines keep working by overriding in the scene inspector; the code default becomes the release URL.

- [x] **Step 3: Commit**

```bash
 git add client/Unity/Assets/Scripts/Runtime
git commit -m "fix(client): point release defaults at production master server"
```

---

## Phase 4 — Dedicated game servers on the mini PC

> Depends on Task 3.2 (publicIp override).

### Task 4.1: Publish + deploy instance 1

**Files:** `/srv/sloparena/server/{SlopArena.Server, server.json, arenas/*.arena}` (mini PC).

- [x] **Step 1: Publish linux-x64 (framework-dependent) + rsync**

```bash
# dev machine
dotnet publish src/Server/SlopArena.Server.csproj -c Release -r linux-x64 --self-contained false -o build/minipc
ssh alfred 'sudo mkdir -p /srv/sloparena/server && sudo chown alfred:alfred /srv/sloparena/server'
rsync -avz build/minipc/ alfred:/srv/sloparena/server/   # build-release.sh deletes server.json from publish output — never clobber the live one
mkdir -p /tmp/arenas && cp data/arenas/*.arena /tmp/arenas/
rsync -avz /tmp/arenas/ alfred:/srv/sloparena/server/arenas/
```

- [x] **Step 2: Write `/srv/sloparena/server/server.json`** (camelCase — Task 3.2):

```json
{
  "serverName": "SLOPArena EU #1",
  "region": "EU",
  "port": 7777,
  "maxConcurrentMatches": 4,
  "masterServerUrl": "https://sloparena.barakaslurp.fr",
  "isOfficial": true,
  "arenaDataDir": "/srv/sloparena/server/arenas",
  "publicIp": "sloparena.barakaslurp.fr"
}
```

> Deployed value is `maxConcurrentMatches: 4` (Bbox forwards only cover UDP 7777-7780; 15 would need 7777-7791).

### Task 4.2: docker compose service

**Files:** `server-1` service in `/root/homelab/sloparena/docker-compose.yml` (deployed in Task 2.2).

- [x] **Step 1: Start the service**

```bash
ssh alfred 'cd /root/homelab/sloparena && docker compose up -d server-1'
```

The `server-1` service runs `mcr.microsoft.com/dotnet/aspnet:8.0` with `network_mode: host` — TCP 7777 + UDP 7777-7791 bind directly on the host (no docker port publishing, no iptables for the UDP range), same resource caps as the old unit (`mem_limit: 512m`, `cpus: 0.5`), config + arenas bind-mounted read-only.

- [x] **Step 2: Verify registration**

```bash
ssh alfred 'cd /root/homelab/sloparena && docker compose logs server-1 | tail -30'   # registration + heartbeat lines
docker exec sloparena-postgres psql -U sloparena -d sloparena -c "SELECT name, \"ipAddress\", port, \"isOfficial\" FROM \"GameServers\";"
# ipAddress = sloparena.barakaslurp.fr, isOfficial = true
```

- [ ] **Step 3: Optional second instance** (only after Phase 7 playtest shows demand): add a `server-2` service in the compose file with `port: 7877`, `serverName: "SLOPArena EU #2"`, UDP forward `7877-7891`. Not required for the friends demo.

---

## Phase 5 — Build & release pipeline

### Task 5.1: Player identity

**Files:** Modify `client/Unity/ProjectSettings/ProjectSettings.asset`.

- [ ] **Step 1: Set productName + version**

```
productName: SLOPArena
companyName: SLOPArena
bundleVersion: 0.1.0
```

Re-open Unity once so the editor regenerates any dependent metadata; confirm the Window title reads "SLOPArena".

- [ ] **Step 2: Commit**

```bash
git add client/Unity/ProjectSettings/ProjectSettings.asset
git commit -m "chore: set player product name and version"
```

### Task 5.2: Gitignore staged build artifacts

**Files:** Modify `.gitignore`.

- [ ] **Step 1: Append**

```
# Staged by scripts/build-release.sh (bundled server + arenas for player build)
/client/Unity/Assets/StreamingAssets/
```

### Task 5.3: `scripts/build-release.sh`

**Files:** Create `scripts/build-release.sh` (executable).

- [ ] **Step 1: Write the script**

```bash
#!/usr/bin/env bash
# Build a Windows demo release: SlopArena-<version>.zip in build/release/.
# Usage: scripts/build-release.sh <version>   e.g. scripts/build-release.sh 0.2.0-demo.1
set -euo pipefail

VERSION="${1:?usage: build-release.sh <version>}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
UNITY="${UNITY_EDITOR:-/home/binoui/Unity/Hub/Editor/6000.0.78f1/Editor/Unity}"
PROJ="$ROOT/client/Unity"
REL="$ROOT/build/release/SlopArena-$VERSION"
SA="$PROJ/Assets/StreamingAssets"

echo "== Shared build =="
dotnet build "$ROOT/src/Shared/" --nologo

echo "== Tests =="
dotnet test "$ROOT/tests/Shared.Tests/" --nologo

echo "== Self-contained Windows server (embedded host-and-play) =="
dotnet publish "$ROOT/src/Server/SlopArena.Server.csproj" -c Release -r win-x64 --self-contained true -o "$SA/Server"

echo "== linux-x64 server for the mini PC =="
dotnet publish "$ROOT/src/Server/SlopArena.Server.csproj" -c Release -r linux-x64 --self-contained false -o "$ROOT/build/minipc"

echo "== Stage baked data (arenas + skeleton bins) =="
mkdir -p "$SA/arenas" "$SA/data" "$SA/Server/data"
cp "$ROOT"/data/arenas/*.arena "$SA/arenas/"
# Skeleton bins: the client reads them from StreamingAssets/data (BakedContentPaths),
# the bundled server from its CWD-relative data/ (MatchInstance.LoadBakedData runs with
# WorkingDirectory = the server binary dir). Issue #77: without this, bone-attached
# hitboxes degrade to capsules in the shipped exe.
cp "$ROOT"/data/*.bin "$SA/data/"
cp "$ROOT"/data/*.bin "$SA/Server/data/"

echo "== Version stamp =="
sed -i "s/^bundleVersion: .*/bundleVersion: $VERSION/" "$PROJ/ProjectSettings/ProjectSettings.asset"

echo "== Unity Windows player build =="
mkdir -p "$REL"
"$UNITY" -batchmode -quit -projectPath "$PROJ" -buildWindows64Player "$REL/SlopArena.exe"

echo "== Restore committed bundleVersion =="
git -C "$ROOT" checkout -- client/Unity/ProjectSettings/ProjectSettings.asset

echo "== Unstage build-only artifacts =="
rm -rf "$SA/Server" "$SA/arenas" "$SA/data"

echo "== Ship docs + zip =="
cp "$ROOT/docs/release/PLAY_GUIDE.md" "$REL/README.txt"
cp "$ROOT/docs/release/HOST_GUIDE.md" "$REL/HOSTING.txt"
(cd "$REL/.." && zip -r "SlopArena-$VERSION.zip" "SlopArena-$VERSION")
echo "DONE: build/release/SlopArena-$VERSION.zip"
```

```bash
chmod +x scripts/build-release.sh
```

- [ ] **Step 2: Dry-run the mechanical parts** (no Unity): run with a fake version and confirm Shared/test/publish steps + staging + cleanup behave; then delete the partial `build/release/SlopArena-fake/` output.

- [ ] **Step 3: Commit**

```bash
git add scripts/build-release.sh .gitignore
git commit -m "build: add release build script (issue #52)"
```

### Task 5.4: CI test gate

**Files:** Create `.github/workflows/ci.yml` (this repo).

- [ ] **Step 1: Workflow**

```yaml
name: ci
on:
  push:
    branches: [main]
  pull_request:
jobs:
  test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'
      - run: dotnet build src/Shared/ --nologo
      - run: dotnet test tests/Shared.Tests/ --nologo
      - run: dotnet build src/Server/ --nologo
```

- [ ] **Step 2: Master repo CI** — in `SlopArena-MasterServer`, extend the existing workflow: add a test job (`dotnet test`) and, on tag push (`v*`), a publish job that uploads `dotnet publish -c Release` output as a release artifact. Deploy stays manual/scripted (home infra is not CI-reachable).

- [ ] **Step 3: Verify + commit**

```bash
# push to a branch, confirm the workflow turns green in GitHub UI before merging
git add .github/workflows/ci.yml
git commit -m "ci: add test gate for shared + server"
```

### Task 5.5: Release cut

- [ ] **Step 1: Full build**

```bash
scripts/build-release.sh 0.2.0-demo.1
# DONE: build/release/SlopArena-0.2.0-demo.1.zip
```

- [ ] **Step 1b: Master server refresh (separate repo, manual)** — the master repo publishes independently and is NOT part of `build-release.sh`:

```bash
cd ~/Documents/projects/SlopArena-MasterServer
dotnet publish -c Release -o /tmp/minipc-master   # /tmp, NOT build/ — test-project bin/obj leaks + recurses (see Task 2.1 warning)
rsync -avz --exclude 'appsettings.Production.json' /tmp/minipc-master/ alfred:/srv/sloparena/master/publish/   # no --delete — would wipe appsettings.Production.json
ssh alfred 'cd /root/homelab/sloparena && docker compose up -d --force-recreate master'
# only needed when master code changed; the master API is versioned, so the game client is not coupled to its release cadence
```

- [ ] **Step 2: Publish**

```bash
gh release create v0.2.0-demo.1 build/release/SlopArena-0.2.0-demo.1.zip \
  --title "SLOPArena v0.2.0-demo.1" \
  --notes "$(cat docs/release/RELEASE_NOTES.template.md)"
```

(Keep the template in `docs/release/RELEASE_NOTES.template.md`: what's new, how to play, known issues, "friends-only" note.)

---

## Phase 6 — Docs

### Task 6.1: Player guide (non-technical)

**Files:** Create `docs/release/PLAY_GUIDE.md` (shipped as `README.txt` in the zip).

- [x] **Step 1: Write the guide** — full text:

```markdown
# SLOPArena — how to play

1. Download `SlopArena-<version>.zip` from the release page.
2. Right-click the zip → Extract All. Keep the extracted folder together.
3. Open the folder and double-click `SlopArena.exe`.
   - Windows shows "Windows protected your PC" → click "More info" → "Run anyway".
     (The game isn't code-signed yet; this is normal.)
4. Main menu:
   - **Training** — play solo against bots, try every character.
   - **Join** — pick a server (e.g. "SLOPArena EU #1"), enter the lobby, ready up.
     Your friend does the same; the host starts when everyone is ready.
5. Controls:
   - Mouse — aim camera
   - WASD — move
   - Space — jump (double-tap for double jump)
   - Shift — dash
   - Left click — attack
   - Right click — ability 1
   - Q / E / R / F — abilities 2-5
   - Esc — pause / back
6. Problems?
   - Game won't start → your PC must be 64-bit Windows 10/11.
   - Firewall prompt → allow SLOPArena on private networks.
   - Can't see any servers → the online part is a friends demo; ask the host
     if the servers are up.
```

- [x] **Step 2: Commit**

```bash
 git add docs/release/PLAY_GUIDE.md docs/release/HOST_GUIDE.md docs/release/RELEASE_NOTES.template.md
git commit -m "docs: add player guide, host guide, release notes template"
```

### Task 6.2: Host guide (technical players)

**Files:** Create `docs/release/HOST_GUIDE.md` (shipped as `HOSTING.txt`).

- [x] **Step 1: Write it**

```markdown
# SLOPArena — hosting your own game

Hosting starts an embedded game server on your machine. Others can join you
over the internet ONLY if your router forwards traffic to you.

1. Router: forward TCP 7777 and UDP 7777-7791 to this PC.
2. Check your public IP (https://ifconfig.me) and confirm your ISP doesn't
   use CGNAT (public IP must equal your router's WAN IP).
3. In the game, click Host and enter your public IP or domain.
4. Share the server name — friends find it in the Join list.

The game server is bundled inside the game — no .NET or extra installs needed.
```

(Note: the host UI field for public IP is wired through `HostedServerConfig.PublicIp` — if the field isn't in the UI yet, add a text field to the Host screen in this task's implementation pass.)

### Task 6.3: Operator runbook + release pipeline docs

**Files:**
- Create `docs/systems/production-hosting.md`
- Create `docs/systems/release-pipeline.md`
- Create `docs/systems/troubleshooting.md` (added post-plan, 2026-08-02, after the first live playtest: UFW drops, stale registration, rsync config wipes, Unity build failures — every failure mode hit during deploy)

- [x] **Step 1: production-hosting.md** — the mini-PC runbook: condensed Phases 0-4 (CGNAT check, port forwards, postgres container + `.env` secret, master publish+rsync, docker compose services, Cloudflare Tunnel hostname, backups, DNS-IP-change procedure, VPS fallback note, "machine runs home automation — be additive" warning).

- [x] **Step 2: release-pipeline.md** — how to cut a release: version scheme (`v<major>.<minor>.<patch>-demo.<n>`), `scripts/build-release.sh`, `gh release create`, CI status, what each artifact is (`build/release/*.zip`, `build/minipc/`).

- [x] **Step 3: Record Phase 0 results** in `production-hosting.md` (CGNAT verdict, public IP, DNS record, forwarded ports) — closes Task 0.4's stub.

- [x] **Step 4: Commit**

```bash
 git add docs/systems/production-hosting.md docs/systems/release-pipeline.md
git commit -m "docs: add production hosting runbook and release pipeline"
```

---

## Phase 7 — Demo readiness gate

### Task 7.1: Remote playtest

- [x] **Step 1: Run the full pipeline once** (Tasks 5.3-5.5) → zip distributed to 2 friends.
  (Build + zip verified 2026-08-02: `build/release/SlopArena-0.2.0-demo.1.zip`, 90MB. Not yet published to GitHub / distributed — `gh release create` awaits operator go.)
- [ ] **Step 2: Scripted playtest checklist** (operator):

| Check | Pass |
|---|---|
| Fresh Windows machine: unzip, SmartScreen, launch | ☐ |
| Training mode solo (no network) | ☐ |
| Join "SLOPArena EU #1", lobby, char select, fight | ☐ |
| 2nd player joins, 2v2 roster | ☐ |
| 15min match — visible jitter/desync? | ☐ |
| Host-and-play (technical friend, port-forwarded) | ☐ |
| Master survives game-server restart (`systemctl restart sloparena-server-1`) | ☐ |

- [ ] **Step 3: Jitter verdict** — record ping + perceived smoothness. If remote play is poor → reconciliation is required for the real demo release:

  - Write `docs/plans/2026-08-XX-prediction-rollback.md` (client-side prediction + rollback per `docs/systems/netcode-architecture.md` §6 Phase 7: prediction ring buffer, input echo matching, snapshot reconciliation; `NetworkSimulationBridge` → `PredictedSimulationBridge`).
  - The friends demo may ship without it (Q4 decision: "reconciliation before *actual* demo release").

### Task 7.2: Clean-machine rehearsal

- [ ] **Step 1: Fresh Windows VM/laptop** (no Unity, no .NET, no repo). Install from the zip only.
- [ ] **Step 2: Verify no dev dependencies leak** — `localhost:5000` must appear NOWHERE (grep the zip's scripts; watch the client log while joining; check the host flow spawns the bundled exe, not `dotnet`).

  Verified at build time (2026-08-02): full-text + binary scan of the zip → 0 hits for `localhost:5000` and `127.0.0.1:5000`; `server.json` removed from the bundled server publish output (`build-release.sh` rm); `ServerHost` `useBundled` path spawns `StreamingAssets/Server/SlopArena.Server.exe` on WindowsPlayer (statically confirmed, file present in zip). Remaining: runtime confirmation on a clean machine.

---

## Self-review

- **Spec coverage:** all grill decisions mapped — Q1→ADR-0009 + Phase 3.3/4, Q2→Phases 1-2, Q3→Task 2.3, Q4→Phase 7, Q5→Global Constraints + server.json, Q6/Q7→player guide + GitHub Releases, Q8→Task 5.1/5.3, Q9→Task 5.4, Q10→Phase 0 gate + resource caps. `ServerHost` csproj-spawn blocker → Task 3.3. snake_case binding bug → Task 3.2. LAN-IP registration bug → Task 3.2 + 4.1.
- **Placeholder scan:** no TBDs; every task has exact commands/configs/code and a verification step.
- **Type consistency:** `PublicIp` flows identically through `ServerConfig` (server.json key `publicIp`), `HostedServerConfig` (camelCase `publicIp`), `GameServerRegistration` (`_config.PublicIp ?? GetPublicIpAddress()`), and the HostedServerConfigTests assertion. `bundleVersion` sed matches the ProjectSettings YAML format. Server binary name `SlopArena.Server[.exe]` matches the csproj assembly name and `Program.cs` arg[0] contract.
