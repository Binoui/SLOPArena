# Historical home hosting — Alfred

This records the 2026-08-02 Alfred home setup. The current Steam Playtest uses
the VPS and [coordinated release pipeline](release-pipeline.md); use
[the VPS runbook](../../deploy/vps/README.md) for current logs and operations.
Alfred's commands/paths below are historical and have not been revalidated for
the current release. Follow them only for an explicitly selected home target,
after checking its current homelab configuration.

The historical backend ran Master, PostgreSQL and GameServer in Docker on a
Debian 12 mini PC. Master HTTPS used outbound Cloudflare Tunnel; gameplay used
router-forwarded raw UDP and private match control. This topology does not
describe the Steam-only VPS.

> **Broken?** See `docs/systems/troubleshooting.md` first — it is the
diagnostic playbook for every failure mode below (UFW drops, stale
registration, rsync config wipes, build failures).

> **Warning:** this machine runs the user's home automation (Home Assistant,
> Plex, cloudflared). Every step here is additive — never touch existing
> containers, configs, or data. The app database is its own `postgres:15`
> container with a named volume.

## Topology

```
Bbox router (NAT: TCP 7777, UDP 7777-7780 → <alfred-lan-ip>)
   │
   ▼
alfred (<alfred-lan-ip>, wired NIC)
   ├─ sloparena-postgres  (127.0.0.1:5432, bridge)
   ├─ sloparena-master    (127.0.0.1:5000, host network)
   ├─ sloparena-server-1  (TCP/UDP 7777, host network)
   └─ cloudflared         (tunnel → sloparena.barakaslurp.fr → localhost:5000)
```

## Phase 0 results (2026-08-02, recorded 2026-08-02)

- **CGNAT: NOT present.** Public IP `<public-ip>` == router WAN IP
  (verified via UPnP IGD `ExternalIPAddress`). No VPS fallback needed.
- **DNS:** no A record — Cloudflare Tunnel provisions DNS (CNAME) for
  `sloparena.barakaslurp.fr` automatically.
- **Forwarded ports** (Bbox admin → NAT/PAT): TCP `7777` and UDP `7777-7780`
  → `<alfred-lan-ip>` (alfred). Range is capped by `maxConcurrentMatches: 4`
  on the official server — widening to `7777-7791` requires a server.json +
  router change.
- **Static DHCP lease:** bind `<alfred-lan-ip>` to alfred's wired NIC MAC
  (Bbox admin → DHCP).

## Layout on the box

| Path | Purpose |
|---|---|
| `/root/homelab/sloparena/docker-compose.yml` | Compose stack (source of truth, versioned on the box — deliberately NOT in the game repo) |
| `/root/homelab/sloparena/.env` | `SLOPARENA_DB_PASSWORD` (chmod 600) |
| `/srv/sloparena/master/publish/` | Master binaries + `appsettings.Production.json` (chmod 600, inside publish/) |
| `/srv/sloparena/server/` | Game server binaries + `server.json` + `arenas/` |
| `/var/backups/sloparena/` | Weekly pg_dump (cron) |
| `/etc/cron.d/sloparena-backup` | `30 4 * * 1` pg_dump → gzip, keep 90 days |

## Common operations

### Status

```bash
ssh alfred 'cd /root/homelab/sloparena && docker compose ps'
# sloparena-postgres, sloparena-master, sloparena-server-1 all Up
curl -s https://sloparena.barakaslurp.fr/health
# {"status":"ok","version":"0.1.0"}
```

### Firewall (UFW) — game ports MUST be allowed

alfred runs UFW with `INPUT policy DROP` (home-automation host). The Bbox
forward is NOT enough — packets reach alfred and get dropped by UFW. Without
this, players see "join hangs / times out" (diagnosed 2026-08-02: client log
showed `[ServerBrowser] Joining server: ...` then silence).

```bash
ssh alfred 'sudo ufw allow 7777/tcp && sudo ufw allow 7777:7791/udp'
# verify: sudo ufw status | grep 7777
```

> Rule must match `server.json` port + `maxConcurrentMatches` (UDP 7777-7780
> for 4 matches; 7777-7791 for 15). Widening one without the other breaks
> matches beyond the smaller range.

### Connectivity check (from outside alfred)

```bash
# TCP 7777 (match-control channel): 0 = connect OK, 124 = firewall/forward drop
timeout 8 bash -c 'echo > /dev/tcp/<alfred-lan-ip>/7777' && echo OK
# Public path through the Bbox (hairpin) — should also connect:
timeout 8 bash -c 'echo > /dev/tcp/<public-ip>/7777' && echo OK
# UDP 7777 round-trip: python listener on alfred, send from dev
ssh alfred 'python3 -c "import socket;s=socket.socket(socket.AF_INET,socket.SOCK_DGRAM);s.bind((\"0.0.0.0\",7777));s.settimeout(10);print(s.recvfrom(64))"' &
sleep 1.5 && echo -n probe | timeout 8 bash -c 'cat > /dev/udp/<alfred-lan-ip>/7777' && wait
```

### Redeploy master (after code change in SlopArena-MasterServer)

```bash
cd ~/Documents/projects/SlopArena-MasterServer
dotnet publish -c Release -o /tmp/minipc-master   # /tmp, NOT build/ — the
  # MasterServer.Tests subfolder leaks its bin/obj into build/ output and
  # grows recursively on re-publish → MSB3030.
rsync -avz --exclude 'appsettings.Production.json' \
  /tmp/minipc-master/ alfred:/srv/sloparena/master/publish/
# ⚠ NEVER add --delete to that rsync: it wipes appsettings.Production.json.
#   --delete-excluded is EVEN WORSE — it deletes excluded files (happened
#   2026-08-02; JWT secret had to be regenerated).
ssh alfred 'cd /root/homelab/sloparena && docker compose restart master'
```

> Before upgrading the existing home Master binary, add
> `"Deployment": {"Profile": "development"}` to its private
> `appsettings.Production.json`; an unspecified profile now fails startup.
> This does not change the current running home service or opt it into the
> VPS trust profile.

### Redeploy game server (after code change in src/Server)

```bash
cd ~/Documents/projects/SlopArena
dotnet publish src/Server/SlopArena.Server.csproj -c Release \
  -r linux-x64 --self-contained false -o build/minipc
ssh alfred 'sudo mkdir -p /srv/sloparena/server && sudo chown alfred:alfred /srv/sloparena/server'
rsync -avz build/minipc/ alfred:/srv/sloparena/server/   # build-release.sh deletes server.json from the publish output — never clobber the live one
rsync -avz data/arenas/*.arena alfred:/srv/sloparena/server/arenas/
ssh alfred 'cd /root/homelab/sloparena && docker compose restart server-1'
```

> Before upgrading the existing home GameServer binary, add
> `"deploymentProfile": "development"` to its private `server.json`; the new
> binary refuses an unspecified profile. This is not a VPS authorization
> profile. The GameServer retries transient registration failures and
> re-registers if Master loses its record; a restart is still needed when
> replacing binaries, not merely because Master restarted.

### Verify registration

```bash
ssh alfred 'docker exec sloparena-postgres psql -U sloparena -d sloparena -c \
  "SELECT \"Name\", \"IpAddress\", \"Port\", \"IsOfficial\", \"LastHeartbeat\" FROM \"GameServers\";"'
# expect IpAddress = sloparena.barakaslurp.fr, IsOfficial = t, fresh heartbeat
```

### Migrations (master repo, one-time per schema change)

Postgres publishes host-only on 127.0.0.1; tunnel through SSH (dev machine's
own 5432 is taken by its local postgres):

```bash
ssh -f -N -L 15432:127.0.0.1:5432 alfred
cd ~/Documents/projects/SlopArena-MasterServer
export ConnectionStrings__DefaultConnection='Host=localhost;Port=15432;Database=sloparena;Username=sloparena;Password=<from /root/homelab/sloparena/.env>'
dotnet ef database update
```

### DNS / IP change (ISP re-assigns IP)

No action needed for HTTPS (tunnel is outbound). UDP game traffic uses the
domain `sloparena.barakaslurp.fr` — if the box's IP changes, only the
forwarded ports still need to point at the new LAN IP. If CGNAT ever appears,
deploy Phases 1-4 to a VPS (Hetzner CX22, ~4€/mo) and update server.json.

## Backup / restore

Backup (automatic, Mondays 04:30):

```bash
sudo bash -c 'docker exec sloparena-postgres pg_dump -U sloparena sloparena | gzip > /var/backups/sloparena/manual.sql.gz'
```

Restore:

```bash
ssh alfred 'gunzip -c /var/backups/sloparena/<file>.sql.gz | docker exec -i sloparena-postgres psql -U sloparena sloparena'
```

## SSH notes

- `~/.ssh/config` alias `alfred` (Tailnet).
- cloudflared is a token-based (remotely-managed) container: after dashboard
  changes, `cd /root/homelab && sudo docker compose restart cloudflared`.
