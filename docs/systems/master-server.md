# SLOPArena Master Server

> **Status:** Deployed 2026-08-01 (issue [#28](https://github.com/Binoui/SlopArena/issues/28)) — foundation for the [PvP Demo](../plans/2026-08-01-pvp-roadmap-v2.md) epic.
> **Repo:** https://github.com/Binoui/SlopArena-MasterServer

The master server is the matchmaking/meta API for online PvP. It runs separately from the GameHost (`src/Server`): GameHosts register and heartbeat; authenticated clients browse named public Rooms and use the Lobby hub for Room preparation and Server Chat. Master assigns a compatible GameHost only when a Room starts a Match; the physical host identity and gameplay route are distinct from the Room ID.

---

## Stack

- ASP.NET Core 8 Web API (minimal API in `Program.cs`)
- PostgreSQL via EF Core 8 + Npgsql
- Bearer-token auth: development `/servers/register` issues a random GameHost API token; VPS registration requires the provisioned registration key. Master stores only the API token's SHA-256 digest and authenticates heartbeat/result/cancel by digest and timing-safe comparison. JWT authentication is used for Steam clients (development mode also permits guest JWTs); GameHost credentials are not client credentials.
- SignalR Lobby hub serves public Rooms, Room-scoped Server Chat and match preparation; development-only physical host/address workflows remain separate.

---

## Local deployment

**Prerequisites:** PostgreSQL (tested on 18.4; Npgsql EF Core 8 supports 13+) running on `localhost:5432`, .NET 8 SDK + runtime, `dotnet-ef` 8.0.0.

```bash
# Clone (already at ~/Documents/projects/SlopArena-MasterServer on this machine)
git clone https://github.com/Binoui/SlopArena-MasterServer

cd SlopArena-MasterServer

# Create the database once (trust auth in dev; password in the connection string is ignored)
psql -U postgres -h localhost -c "CREATE DATABASE sloparena;"

# Apply EF Core migrations → tables: GameServers, Users, Matches
ASPNETCORE_ENVIRONMENT=Development dotnet ef database update

# Start the server (Development profile → http://localhost:5000)
ASPNETCORE_ENVIRONMENT=Development dotnet run --no-build --urls http://localhost:5000
```

**Master server URL:** `http://localhost:5000`

---

## Configuration

| Where | Key | Dev value |
|---|---|---|
| `appsettings.Development.json` | `ConnectionStrings:DefaultConnection` | `Host=localhost;Port=5432;Database=sloparena;Username=postgres;Password=password` |
| `.env` (gitignored, overrides appsettings when sourced) | `ConnectionStrings__DefaultConnection`, `Jwt__Secret` | dev values; see `.env.example` |
| **this repo** `src/Server/server.json` | `masterServerUrl` | `http://localhost:5000` |
| **this repo** `src/Server/MultiMatchOrchestrator.cs` → `ServerConfig.MasterServerUrl` | default | `http://localhost:5000` |

The game server (`src/Server`) points at the master server via `ServerConfig.MasterServerUrl`, loaded from `server.json`. Change `masterServerUrl` there to redirect registration/heartbeats (e.g. staging).

---

## Endpoints

| Method | Path | Auth | Returns |
|---|---|---|---|
| GET | `/health` | none | `{ "status": "ok", "version": "0.1.0" }` |
| POST | `/auth/guest` | none (issues JWT) | `{ "token": "<jwt>", "steamId": <long> }` |
| GET | `/auth/me` | Bearer JWT | `{ "steamId": <long>, "username": "<string>", "mmr": <int> }` |
| POST | `/servers/register` | VPS registration key; development none | `{ "serverId": "<guid>", "apiToken": "<secret>" }` |
| POST | `/servers/{serverId}/heartbeat` | Bearer `apiToken` | `{ "status": "ok" }` |
| POST | `/match/result` | GameHost `apiToken` | Records the Match result once and returns its matching active Room to Lobby |
| POST | `/match/cancel` | GameHost `apiToken` | Cancels an open Match without a winner and returns its matching active Room to Lobby |
| GET | `/servers` | Bearer JWT, development only | Physical GameHost lookup for explicit development host/address workflows; production returns 404 |

The normal browser uses `GetRooms()` on the authenticated Lobby hub, not `/servers`.

### Room directory (Unity Server Browser)

The authenticated Lobby hub exposes `GetRooms()`, `CreateRoom(name)`,
`JoinRoom(roomId)`, `LeaveRoom()`, and `GetMyRoom()`. The normal browser lists
only public Rooms; a Room is a named, up-to-four-player lobby and does not
allocate or start a GameHost. `RoomDirectoryChanged` tells connected browser
clients to re-read `GetRooms()` after summaries change; it carries no Room
chat or Match route. `RoomUpdated` supplies the authoritative member roster
only to that Room; `RoomDeleted` signals an empty Room's expiry.
The leader calls `RoomStartCharacterSelect()` (with at least two members);
members lock in via `RoomSelectCharacter(selector)`.
The leader then calls `RoomStartStageSelect()`
once all are locked and `RoomChooseArena(name)` to finish preparation. The
Room snapshot holds `Lobby` / `Character Select` / `Stage Select` /
`Match Starting` / `In Match`, ordered members with Character selection and
Lock-in, a chosen Arena, and optional active Match ID. Only Lobby Rooms admit
new members. The leader's `RoomStartMatch()` allocates a compatible GameHost
at launch time; the Room group alone receives `MatchStarted`, with the physical
Server ID and authoritative content/Steam descriptor. Master pins
`Room:AdmittedCharacters`, `Room:AdmittedArenas`, and the deployed
`Room:CatalogHash`; the GameHost independently validates the content.
The durable Match row has nullable `RoomId` alongside physical `ServerId`.
Failed launches return to Stage Select with picks intact. Authenticated result
or cancellation advances only the Room whose active Match ID matches, once,
back to Lobby, clearing Arena and Character Lock-in while preserving membership
and Server Chat. A stale report cannot reset a later rematch. Results may remain
on screen until the player chooses to return; a departed/expired member returns
to the browser with an explanation. Host restart, shutdown and absent-player
cancellation do not invent a winner. A host with no heartbeat for at least
60 seconds is probed through its control `/health`; only a failed probe with
the host still stale cancels its open Matches as `host_unavailable`. A stale
heartbeat alone is not evidence that an admitted fight stopped.

Physical GameHost lookup and explicit development host/address flows remain
separate; Room membership is neither a physical-server lobby nor a gameplay
connection.

### Smoke test

```bash
# Health
curl -s http://localhost:5000/health
# → {"status":"ok","version":"0.1.0"}

# Register a fake game server
curl -s -X POST http://localhost:5000/servers/register \
  -H "Content-Type: application/json" \
  -d '{"name":"fake-eu-1","ipAddress":"127.0.0.1","port":9876,"region":"eu-west","isOfficial":false,"maxConcurrentMatches":15,"customRulesJson":null}'
# → {"serverId":"...","apiToken":"..."}

# Guest auth — get a JWT + temporary SteamId
curl -s -X POST http://localhost:5000/auth/guest
# → {"token":"<jwt>","steamId":12345678}

# Use the JWT to hit an authed endpoint
curl -s http://localhost:5000/auth/me \
  -H "Authorization: Bearer <jwt>"
# → {"steamId":12345678,"username":"Guest-12345","mmr":1000}

# Without the JWT → 401
curl -s http://localhost:5000/auth/me
# → 401 Unauthorized
```

---

## Database schema

- `GameServers` — registered GameHosts (identity, address/control port, catalog hash, API-token digest, heartbeat and Match capacity).
- `Users` — authenticated player identities, display names and account state.
- `Matches` — durable roster and Match outcomes; nullable `RoomId` is distinct from physical `ServerId`, and canceled rows store no winner.

---

## Notes

- The `Jwt__Secret` in `.env` signs guest JWTs (HMAC-SHA256). Dev-only; regenerate with `openssl rand -base64 64` for any non-local deployment.
